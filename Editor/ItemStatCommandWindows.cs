using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Small shared pieces of the CommandList part 2 windows.</summary>
internal static class CommandFields
{
    public static ComboBox Who(StackPanel rows, string? existing, bool includePlayer, string? fallback = null)
    {
        var choices = WhoChoice.Build(includePlayer);
        var combo = new ComboBox { ItemsSource = choices };
        combo.SelectedItem = choices.FirstOrDefault(c => c.Key == existing) ?? choices.FirstOrDefault(c => c.Key == fallback) ?? choices[0];
        rows.Children.Add(combo);
        return combo;
    }

    public static string WhoKey(ComboBox combo) => (combo.SelectedItem as WhoChoice)?.Key ?? WhoChoice.Activator;

    public static TextBox Number(Panel parent, string? existing, string fallback, double width = 60)
    {
        var box = new TextBox { Width = width, Text = existing is { Length: > 0 } v ? v : fallback, TextAlignment = TextAlignment.Right };
        parent.Children.Add(box);
        return box;
    }

    public static TextBox Line(StackPanel rows, string? existing, string fallback)
    {
        var box = new TextBox { Text = existing ?? fallback, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
        rows.Children.Add(box);
        return box;
    }

    public static StackPanel Row(StackPanel rows)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        rows.Children.Add(row);
        return row;
    }

    public static void Note(Panel parent, string text) =>
        parent.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Margin = new Thickness(6, 0, 0, 0) });

    public static List<StatDefinition> Stats() => ProjectSession.StatsProvider?.Invoke()?.Stats ?? new List<StatDefinition>();

    public static string StatName(string id) =>
        id == "All" ? "every stat" : Stats().FirstOrDefault(s => s.Id.ToString() == id)?.Name ?? $"stat #{id}";

    public static string ItemName(EventCommand command)
    {
        if (!int.TryParse(command.Get("ItemId"), out int id) || !Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category)) return "(no item)";
        return DollLook.ItemName(new ItemReference { Category = category, ItemId = id },
            ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase(), ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase());
    }
}

/// <summary>
/// Give Item: one character hands an item to another. "Must have real
/// object" takes it out of the giver's own things (it fails if they haven't
/// got it); otherwise the item is simply given. A receiver who can't take it
/// (too heavy for a party member, no room in pack or hands) gets it from a
/// party member who can, in turn; nobody can - it is put down at the
/// receiver's feet. The lines are spoken in a dialog box and can be changed,
/// for everyone or for one character.
/// </summary>
public class GiveItemCommandWindow : FactionCommandWindowBase
{
    /// <summary>The lines: parameter key, label, who says it, the default.</summary>
    internal static readonly (string Key, string Label, string Default)[] Lines =
    {
        ("CantCarryText", "Receiver can't carry it (said by the giver, not in the party)", "Thou canst not carry this. I shall leave it here at thy feet."),
        ("PartyTryText", "Another party member takes it instead (said by that member)", "I shall carry it."),
        ("NobodyText", "Nobody in the party can carry it (said by the giver)", "None of you can carry this. I shall leave it on the ground."),
        ("NotHaveText", "The giver hasn't got it (Must have real object)", "")
    };

    private readonly ComboBox _giver, _receiver;
    private readonly Button _itemButton;
    private readonly TextBox _quantity;
    private readonly CheckBox _mustHave;
    private readonly Dictionary<string, TextBox> _lines = new();
    private readonly Dictionary<string, string> _overrides = new(); // "Line_{key}_{characterId}" -> text
    private readonly ListBox _overrideList;
    private ItemReference? _item;

    public GiveItemCommandWindow(EventCommand? existing = null) : base("Give Item")
    {
        Width = 520;
        AddLabel("Giver");
        _giver = CommandFields.Who(Rows, existing?.Get("Giver"), includePlayer: true);
        AddLabel("Gives to");
        _receiver = CommandFields.Who(Rows, existing?.Get("Receiver"), includePlayer: true, fallback: WhoChoice.Player);

        AddLabel("Item");
        if (existing is not null && int.TryParse(existing.Get("ItemId"), out int itemId) && Enum.TryParse<ReferencedItemCategory>(existing.Get("Category"), out var category))
            _item = new ItemReference { Category = category, ItemId = itemId, DisplayName = CommandFields.ItemName(existing) };
        var itemRow = CommandFields.Row(Rows);
        _itemButton = new Button { Padding = new Thickness(8, 2, 8, 2), MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Left };
        _itemButton.Click += (_, _) =>
        {
            var picker = new SelectItemPickerWindow(ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase(),
                ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase(), ProjectSession.Sprites, _item) { Owner = this };
            if (picker.ShowDialog() == true && picker.Result is { ItemId: >= 0 } r) _item = new ItemReference { Category = r.Category, ItemId = r.ItemId, DisplayName = r.DisplayName };
            ShowItem();
        };
        itemRow.Children.Add(_itemButton);
        itemRow.Children.Add(new TextBlock { Text = "   How many", VerticalAlignment = VerticalAlignment.Center });
        _quantity = CommandFields.Number(itemRow, existing?.Get("Quantity"), "1", 50);
        _quantity.Margin = new Thickness(6, 0, 0, 0);
        ShowItem();

        _mustHave = new CheckBox { Content = "Must have real object?", IsChecked = existing?.Get("MustHave") == "True", Margin = new Thickness(0, 8, 0, 0) };
        Rows.Children.Add(_mustHave);
        AddLabel("Checked: the giver must really have it - it leaves their things (from a bag, a hand, anywhere they carry it). Not checked: it is given without the giver owning it.", bold: false);

        foreach (var (key, label, fallback) in Lines)
        {
            AddLabel(label);
            _lines[key] = CommandFields.Line(Rows, existing?.Parameters.TryGetValue(key, out var text) == true ? text : null, fallback);
        }

        AddLabel("A different line for one character");
        if (existing is not null)
            foreach (var (key, value) in existing.Parameters.Where(p => p.Key.StartsWith("Line_"))) _overrides[key] = value;
        var who = new ComboBox { ItemsSource = WhoChoice.Build(includePlayer: true).Skip(1).ToList(), Width = 140 };
        if (who.Items.Count > 0) who.SelectedIndex = 0;
        var which = new ComboBox { Width = 110, Margin = new Thickness(4, 0, 4, 0) };
        foreach (var (key, label, _) in Lines) which.Items.Add(new ComboBoxItem { Content = ShortLabel(key), Tag = key, ToolTip = label });
        which.SelectedIndex = 0;
        var said = new TextBox { Width = 150 };
        var add = new Button { Content = "Set", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
        var overrideRow = CommandFields.Row(Rows);
        overrideRow.Children.Add(who);
        overrideRow.Children.Add(which);
        overrideRow.Children.Add(said);
        overrideRow.Children.Add(add);
        _overrideList = new ListBox { Height = 70, Margin = new Thickness(0, 4, 0, 0) };
        Rows.Children.Add(_overrideList);
        var remove = new Button { Content = "Remove selected", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 4, 0, 0) };
        Rows.Children.Add(remove);
        add.Click += (_, _) =>
        {
            if (who.SelectedItem is not WhoChoice character || which.SelectedItem is not ComboBoxItem { Tag: string key }) return;
            _overrides[$"Line_{key}_{character.Key}"] = said.Text;
            ShowOverrides();
        };
        remove.Click += (_, _) =>
        {
            if (_overrideList.SelectedItem is ListBoxItem { Tag: string key }) _overrides.Remove(key);
            ShowOverrides();
        };
        AddLabel("Leave a line empty and nothing is said there.", bold: false);
        ShowOverrides();
    }

    private static string ShortLabel(string key) => key switch
    {
        "CantCarryText" => "Can't carry",
        "PartyTryText" => "Takes it instead",
        "NobodyText" => "Nobody can",
        _ => "Hasn't got it"
    };

    private void ShowItem() => _itemButton.Content = _item is { ItemId: >= 0 } ? _item.DisplayName : "(pick an item...)";

    private void ShowOverrides()
    {
        _overrideList.Items.Clear();
        foreach (var (key, text) in _overrides.OrderBy(o => o.Key))
        {
            var parts = key.Split('_'); // Line_{line}_{characterId}
            if (parts.Length < 3) continue;
            _overrideList.Items.Add(new ListBoxItem { Content = $"{WhoChoice.Describe(parts[2])} - {ShortLabel(parts[1])}: \"{text}\"", Tag = key });
        }
    }

    protected override bool TryBuild()
    {
        if (_item is not { ItemId: >= 0 } item)
        {
            MessageBox.Show(this, "Pick the item to give.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!int.TryParse(_quantity.Text.Trim(), out int quantity) || quantity < 1 || quantity > ObjectTypes.MaxQuantity)
        {
            MessageBox.Show(this, $"How many must be a whole number from 1 to {ObjectTypes.MaxQuantity}.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        string giver = CommandFields.WhoKey(_giver), receiver = CommandFields.WhoKey(_receiver);
        if (giver == receiver)
        {
            MessageBox.Show(this, "The giver and the receiver must be two different characters.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = new EventCommand { Type = EventCommandType.GiveItem };
        command.Parameters["Giver"] = giver;
        command.Parameters["Receiver"] = receiver;
        command.Parameters["Category"] = item.Category.ToString();
        command.Parameters["ItemId"] = item.ItemId.ToString();
        command.Parameters["Quantity"] = quantity.ToString();
        command.Parameters["MustHave"] = _mustHave.IsChecked == true ? "True" : "False";
        foreach (var (key, box) in _lines) command.Parameters[key] = box.Text;
        foreach (var (key, text) in _overrides) command.Parameters[key] = text;
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string quantity = command.Get("Quantity") is { Length: > 0 } q && q != "1" ? q + " x " : "";
        return $"{WhoChoice.Describe(command.Get("Giver"))} gives {quantity}{CommandFields.ItemName(command)} to {WhoChoice.Describe(command.Get("Receiver"))}{(command.Get("MustHave") == "True" ? " (must have it)" : "")}";
    }
}

/// <summary>
/// Change Stat: sets, raises or lowers one stat of someone - for ever, or
/// for a number of game minutes / hours, after which the change is taken
/// back. Reset Stat takes back every Change Stat at once.
/// </summary>
public class ChangeStatCommandWindow : FactionCommandWindowBase
{
    private readonly ComboBox _who, _stat, _operation, _unit;
    private readonly TextBox _value, _duration;
    private readonly CheckBox _temporary;

    public ChangeStatCommandWindow(EventCommand? existing = null) : base("Change Stat")
    {
        AddLabel("Who");
        _who = CommandFields.Who(Rows, existing?.Get("Who"), includePlayer: true);

        AddLabel("Stat");
        var stats = CommandFields.Stats();
        _stat = new ComboBox { ItemsSource = stats, DisplayMemberPath = nameof(StatDefinition.Name) };
        _stat.SelectedItem = stats.FirstOrDefault(s => s.Id.ToString() == existing?.Get("StatId")) ?? stats.FirstOrDefault();
        Rows.Children.Add(_stat);

        AddLabel("Change");
        var line = CommandFields.Row(Rows);
        _operation = new ComboBox { Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var op in new[] { "Set to", "Increase by", "Decrease by" }) _operation.Items.Add(op);
        _operation.SelectedIndex = existing?.Get("Operation") switch { "Set" => 0, "Subtract" => 2, _ => 1 };
        line.Children.Add(_operation);
        _value = CommandFields.Number(line, existing?.Get("Value"), "1", 100);
        _stat.SelectionChanged += (_, _) => ShowType();

        _temporary = new CheckBox { Content = "Temporarily", IsChecked = existing?.Get("Temporary") == "True", Margin = new Thickness(0, 10, 0, 0) };
        Rows.Children.Add(_temporary);
        var durationRow = CommandFields.Row(Rows);
        durationRow.Margin = new Thickness(20, 4, 0, 0);
        durationRow.Children.Add(new TextBlock { Text = "for ", VerticalAlignment = VerticalAlignment.Center });
        _duration = CommandFields.Number(durationRow, existing?.Get("Duration"), "30", 50);
        _unit = new ComboBox { Width = 120, Margin = new Thickness(6, 0, 0, 0) };
        _unit.Items.Add("game minutes");
        _unit.Items.Add("game hours");
        _unit.SelectedIndex = existing?.Get("Unit") == "Hours" ? 1 : 0;
        durationRow.Children.Add(_unit);
        void ShowDuration() => durationRow.IsEnabled = _temporary.IsChecked == true;
        _temporary.Checked += (_, _) => ShowDuration();
        _temporary.Unchecked += (_, _) => ShowDuration();
        ShowDuration();
        AddLabel("Not checked: the change is for ever (until a Reset Stat). Checked: when the time is up the stat goes back by exactly this change - anything else that changed it meanwhile (training, a level) is kept. Integer stats never go below 0; lowering Hits hurts (the red flash), and to 0 knocks out or kills as a blow would.", bold: false);
        ShowType();
    }

    private void ShowType()
    {
        bool isText = (_stat.SelectedItem as StatDefinition)?.Type == VariableValueType.String;
        if (isText) _operation.SelectedIndex = 0;
        _operation.IsEnabled = !isText;
        _value.Width = isText ? 200 : 100;
        _value.TextAlignment = isText ? TextAlignment.Left : TextAlignment.Right;
    }

    protected override bool TryBuild()
    {
        if (_stat.SelectedItem is not StatDefinition stat)
        {
            MessageBox.Show(this, "There are no stats - add them in the Stat Editor.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (stat.Type == VariableValueType.Integer && (!int.TryParse(_value.Text.Trim(), out int amount) || amount < 0))
        {
            MessageBox.Show(this, "The value must be a whole number, 0 or more.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        bool temporary = _temporary.IsChecked == true;
        if (temporary && (!int.TryParse(_duration.Text.Trim(), out int duration) || duration < 1))
        {
            MessageBox.Show(this, "How long must be a whole number, 1 or more.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = new EventCommand { Type = EventCommandType.ChangeStat };
        command.Parameters["Who"] = CommandFields.WhoKey(_who);
        command.Parameters["StatId"] = stat.Id.ToString();
        command.Parameters["Operation"] = _operation.SelectedIndex switch { 0 => "Set", 2 => "Subtract", _ => "Add" };
        command.Parameters["Value"] = stat.Type == VariableValueType.Integer ? _value.Text.Trim() : _value.Text;
        command.Parameters["Temporary"] = temporary ? "True" : "False";
        command.Parameters["Duration"] = _duration.Text.Trim();
        command.Parameters["Unit"] = _unit.SelectedIndex == 1 ? "Hours" : "Minutes";
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string op = command.Get("Operation") switch { "Set" => "= ", "Subtract" => "-", _ => "+" };
        string time = command.Get("Temporary") == "True" ? $" for {command.Get("Duration")} game {(command.Get("Unit") == "Hours" ? "hours" : "minutes")}" : "";
        return $"{WhoChoice.Describe(command.Get("Who"))} {CommandFields.StatName(command.Get("StatId"))} {op}{command.Get("Value")}{time}";
    }
}

/// <summary>Reset Stat: every Change Stat on someone's stat (or all their stats) is taken back, for ever ones too - training and levels are kept.</summary>
public class ResetStatCommandWindow : FactionCommandWindowBase
{
    private sealed record StatChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox _who, _stat;

    public ResetStatCommandWindow(EventCommand? existing = null) : base("Reset Stat")
    {
        AddLabel("Who");
        _who = CommandFields.Who(Rows, existing?.Get("Who"), includePlayer: true);
        AddLabel("Stat");
        var choices = new List<StatChoice> { new("All", "(Every stat)") };
        choices.AddRange(CommandFields.Stats().Select(s => new StatChoice(s.Id.ToString(), s.Name)));
        _stat = new ComboBox { ItemsSource = choices };
        _stat.SelectedItem = choices.FirstOrDefault(c => c.Key == existing?.Get("StatId")) ?? choices[0];
        Rows.Children.Add(_stat);
        AddLabel("Takes back every Change Stat made on it - temporary or for ever - as if they never happened. What training or a level added is kept.", bold: false);
    }

    protected override bool TryBuild()
    {
        var command = new EventCommand { Type = EventCommandType.ResetStat };
        command.Parameters["Who"] = CommandFields.WhoKey(_who);
        command.Parameters["StatId"] = (_stat.SelectedItem as StatChoice)?.Key ?? "All";
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{WhoChoice.Describe(command.Get("Who"))} {CommandFields.StatName(command.Get("StatId"))}";
}

/// <summary>
/// Train: a trainer raises someone's stats for training points and money,
/// as Ultima 7's trainers do (their usecode: not enough training points -
/// "thou dost need more experience"; not enough gold - turned away;
/// otherwise both are paid and the stats go up).
/// </summary>
public class TrainCommandWindow : FactionCommandWindowBase
{
    private sealed record CurrencyChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly ComboBox _trainer, _trainee, _currency;
    private readonly TextBox _points, _price, _description, _noExperience, _noMoney;
    private readonly Dictionary<int, TextBox> _gains = new();

    public TrainCommandWindow(EventCommand? existing = null) : base("Train")
    {
        Width = 500;
        AddLabel("Trainer (says the lines)");
        _trainer = CommandFields.Who(Rows, existing?.Get("Trainer"), includePlayer: false);
        AddLabel("Trains");
        _trainee = CommandFields.Who(Rows, existing?.Get("Trainee"), includePlayer: true, fallback: WhoChoice.Player);

        AddLabel("Raises (leave 0 for a stat it doesn't train)");
        var grid = new WrapPanel();
        foreach (var stat in CommandFields.Stats().Where(s => s.Type == VariableValueType.Integer && s.Kind != BuiltInStatKind.TrainingPoints))
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Width = 150, Margin = new Thickness(0, 2, 0, 2) };
            cell.Children.Add(new TextBlock { Text = stat.Name, Width = 90, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _gains[stat.Id] = CommandFields.Number(cell, existing?.Get("Gain_" + stat.Id), "0", 45);
            grid.Children.Add(cell);
        }
        Rows.Children.Add(grid);

        AddLabel("Costs");
        var pointsRow = CommandFields.Row(Rows);
        _points = CommandFields.Number(pointsRow, existing?.Get("Points"), "1", 50);
        CommandFields.Note(pointsRow, "training points");
        var moneyRow = CommandFields.Row(Rows);
        moneyRow.Margin = new Thickness(0, 4, 0, 0);
        _price = CommandFields.Number(moneyRow, existing?.Get("Price"), "0", 50);
        var currencies = ProjectSession.Currencies.Currencies.Select(c => new CurrencyChoice(c.Id, c.Name)).ToList();
        _currency = new ComboBox { Width = 140, ItemsSource = currencies, Margin = new Thickness(6, 0, 0, 0) };
        _currency.SelectedItem = currencies.FirstOrDefault(c => c.Id.ToString() == existing?.Get("CurrencyId")) ?? currencies.FirstOrDefault();
        moneyRow.Children.Add(_currency);
        CommandFields.Note(moneyRow, "(0 = free; the party's money pays)");

        AddLabel("Description of the training (said once it's done)");
        _description = CommandFields.Line(Rows, existing?.Get("Description"), "After a good deal of practice, thou art better than before.");
        AddLabel("Not enough experience (training points)");
        _noExperience = CommandFields.Line(Rows, existing?.Get("NoExperienceText"), "Thou dost need more experience before I can train thee.");
        AddLabel("Not enough money");
        _noMoney = CommandFields.Line(Rows, existing?.Get("NoMoneyText"), "Thou hast not enough gold to pay for the training.");
        AddLabel("Training points are checked first, then the money (Ultima 7's trainers). Only when both are there are they taken and the stats raised. Leave a line empty and nothing is said.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (!int.TryParse(_points.Text.Trim(), out int points) || points < 0 || !int.TryParse(_price.Text.Trim(), out int price) || price < 0)
        {
            MessageBox.Show(this, "The training points and the price must be whole numbers, 0 or more.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = new EventCommand { Type = EventCommandType.Train };
        bool any = false;
        foreach (var (id, box) in _gains)
        {
            if (!int.TryParse(box.Text.Trim(), out int gain) || gain < 0)
            {
                MessageBox.Show(this, "Each raise must be a whole number, 0 or more.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            if (gain == 0) continue;
            command.Parameters["Gain_" + id] = gain.ToString();
            any = true;
        }
        if (!any)
        {
            MessageBox.Show(this, "Raise at least one stat.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (price > 0 && _currency.SelectedItem is not CurrencyChoice)
        {
            MessageBox.Show(this, "A price needs a currency - add one in the Currency Editor.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        command.Parameters["Trainer"] = CommandFields.WhoKey(_trainer);
        command.Parameters["Trainee"] = CommandFields.WhoKey(_trainee);
        command.Parameters["Points"] = points.ToString();
        command.Parameters["Price"] = price.ToString();
        command.Parameters["CurrencyId"] = (_currency.SelectedItem as CurrencyChoice)?.Id.ToString() ?? "-1";
        command.Parameters["Description"] = _description.Text;
        command.Parameters["NoExperienceText"] = _noExperience.Text;
        command.Parameters["NoMoneyText"] = _noMoney.Text;
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        var gains = command.Parameters.Where(p => p.Key.StartsWith("Gain_")).Select(p => $"{CommandFields.StatName(p.Key[5..])} +{p.Value}");
        string price = command.Get("Price") is { Length: > 0 } p && p != "0"
            ? $", {p} {ProjectSession.Currencies.Currencies.FirstOrDefault(c => c.Id.ToString() == command.Get("CurrencyId"))?.Name ?? "money"}" : "";
        return $"{WhoChoice.Describe(command.Get("Trainee"))}: {string.Join(", ", gains)} for {command.Get("Points")} training points{price}";
    }
}

/// <summary>Kill Character: a moment later (0.05 s) they take a fatal blow - the red hit flash - and die, as from any killing blow (their By Death events run, they leave a body).</summary>
public class KillCharacterCommandWindow : FactionCommandWindowBase
{
    private readonly ComboBox _who;

    public KillCharacterCommandWindow(EventCommand? existing = null) : base("Kill Character")
    {
        AddLabel("Who dies");
        _who = CommandFields.Who(Rows, existing?.Get("Who"), includePlayer: true);
        AddLabel("0.05 seconds later they take a fatal blow - you see the hit flash - and die. The main character dying ends the game as any death does (its By Death events decide what happens).", bold: false);
    }

    protected override bool TryBuild()
    {
        var command = new EventCommand { Type = EventCommandType.KillCharacter };
        command.Parameters["Who"] = CommandFields.WhoKey(_who);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => WhoChoice.Describe(command.Get("Who"));
}
