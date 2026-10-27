using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Shop command's window (Engine/ShopModel.cs). Talk: the merchant's first line and the player's choices, a tree -
/// each choice the line he says then, more choices under it, and its goods (each with how many or infinite, its price,
/// its own price line if wanted). Buys: what he buys from the party, and what he pays. Haggle: Serpent Isle's
/// haggling, its tries and the lowest he'll go. Merchant: who talks, the money. Words: every line he says, and the
/// player's own (Yes, No, Back...) - {item}, {price} and {money} fill in.
/// </summary>
public sealed class ShopCommandWindow : FactionCommandWindowBase
{
    private sealed record Thing(ReferencedItemCategory Category, int Id, string Name);

    private readonly ShopDefinition _shop;
    private readonly TreeView _tree = new() { Width = 260, Height = 430 };
    private readonly StackPanel _nodePanel = new() { Margin = new Thickness(10, 0, 0, 0) };
    private readonly List<Thing> _things;

    public ShopCommandWindow(EventCommand? existing = null) : base("Shop")
    {
        Width = 980;
        _shop = (existing?.Get("Shop") is { Length: > 0 } json ? TryRead(json) : null) ?? NewShop();
        var objects = ProjectSession.ObjectsProvider?.Invoke()?.Objects ?? new List<WorldObject>();
        _things = objects.Select(o => new Thing(ReferencedItemCategory.Object, o.Id, o.Name))
            .Concat((ProjectSession.BooksProvider?.Invoke()?.Books ?? new List<BookData>()).Select(b => new Thing(ReferencedItemCategory.Book, b.Id, b.Name + " (book)")))
            .Concat((ProjectSession.ContainersProvider?.Invoke()?.Containers ?? new List<ContainerData>()).Where(c => !c.IsBodyContainer).Select(c => new Thing(ReferencedItemCategory.Container, c.Id, c.Name + " (container)")))
            .OrderBy(t => t.Name).ToList();

        var tabs = new TabControl { Height = 520 };
        tabs.Items.Add(new TabItem { Header = "Talk and goods", Content = TalkTab() });
        tabs.Items.Add(new TabItem { Header = "Buys from the party", Content = BuysTab() });
        tabs.Items.Add(new TabItem { Header = "Haggle", Content = HaggleTab() });
        tabs.Items.Add(new TabItem { Header = "Merchant and money", Content = MerchantTab(objects) });
        tabs.Items.Add(new TabItem { Header = "Words", Content = WordsTab() });
        Rows.Children.Add(tabs);
    }

    private static ShopDefinition? TryRead(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<ShopDefinition>(json); } catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>A new shop starts as the example: weapons (melee, missile), armour and tools.</summary>
    private static ShopDefinition NewShop() => new()
    {
        Root = new ShopNode
        {
            Line = "Excellent! I sell weapons, armour or tools. Which dost thou want?",
            Children =
            {
                new() { Label = "Weapons", Line = "I sell melee or missile weapons.", Children = { new() { Label = "Melee", Line = "I have these:" }, new() { Label = "Missile", Line = "I have these:" } } },
                new() { Label = "Armour", Line = "I have these:" },
                new() { Label = "Tools", Line = "I have these:" }
            }
        }
    };

    protected override bool TryBuild()
    {
        Result = new EventCommand { Type = EventCommandType.Shop };
        Result.Parameters["Shop"] = System.Text.Json.JsonSerializer.Serialize(_shop);
        return true;
    }

    public static string Describe(EventCommand command)
    {
        var shop = TryRead(command.Get("Shop"));
        if (shop is null) return "(not set)";
        int Count(ShopNode n) => n.Items.Count + n.Children.Sum(Count);
        return $"{Count(shop.Root)} goods, buys {shop.Buys.Count}{(shop.Haggle ? ", haggles" : "")}";
    }

    // ---------- Talk ----------

    private FrameworkElement TalkTab()
    {
        var left = new StackPanel();
        left.Children.Add(_tree);
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(Button("Add a choice under it", () =>
        {
            var at = Selected() ?? _shop.Root;
            var child = new ShopNode { Label = "New choice", Line = "I have these:" };
            at.Children.Add(child);
            RefreshTree(child);
        }));
        buttons.Children.Add(Button("Remove", () =>
        {
            if (Selected() is not { } node || ReferenceEquals(node, _shop.Root)) return;
            if (MessageBox.Show(this, $"Remove \"{node.Label}\" and everything under it?", "Shop", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            ParentOf(_shop.Root, node)?.Children.Remove(node);
            RefreshTree(_shop.Root);
        }));
        buttons.Children.Add(Button("Up", () => Move(-1)));
        buttons.Children.Add(Button("Down", () => Move(1)));
        left.Children.Add(buttons);
        _tree.SelectedItemChanged += (_, _) => ShowNode(Selected());
        var grid = new Grid { Margin = new Thickness(6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        var right = new ScrollViewer { Content = _nodePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        RefreshTree(_shop.Root);
        return grid;
    }

    private ShopNode? Selected() => (_tree.SelectedItem as TreeViewItem)?.Tag as ShopNode;

    private static ShopNode? ParentOf(ShopNode root, ShopNode node)
    {
        foreach (var child in root.Children)
        {
            if (ReferenceEquals(child, node)) return root;
            if (ParentOf(child, node) is { } found) return found;
        }
        return null;
    }

    private void Move(int by)
    {
        if (Selected() is not { } node || ParentOf(_shop.Root, node) is not { } parent) return;
        int i = parent.Children.IndexOf(node), j = i + by;
        if (j < 0 || j >= parent.Children.Count) return;
        (parent.Children[i], parent.Children[j]) = (parent.Children[j], parent.Children[i]);
        RefreshTree(node);
    }

    private void RefreshTree(ShopNode select)
    {
        _tree.Items.Clear();
        TreeViewItem Make(ShopNode node, bool root)
        {
            var item = new TreeViewItem { Header = root ? "The merchant's first line" : $"{node.Label}  ({node.Items.Count} goods)", Tag = node, IsExpanded = true };
            foreach (var child in node.Children) item.Items.Add(Make(child, false));
            return item;
        }
        var top = Make(_shop.Root, true);
        _tree.Items.Add(top);
        TreeViewItem? Find(TreeViewItem item) => ReferenceEquals(item.Tag, select) ? item : item.Items.OfType<TreeViewItem>().Select(Find).FirstOrDefault(f => f is not null);
        if (Find(top) is { } found) found.IsSelected = true;
        ShowNode(select);
    }

    private void ShowNode(ShopNode? node)
    {
        _nodePanel.Children.Clear();
        if (node is null) return;
        bool root = ReferenceEquals(node, _shop.Root);
        if (!root)
        {
            _nodePanel.Children.Add(Label("The player's choice (what leads here)"));
            var label = new TextBox { Text = node.Label, Width = 400, HorizontalAlignment = HorizontalAlignment.Left };
            label.LostFocus += (_, _) => { node.Label = label.Text; RefreshTreeHeaderOnly(); };
            _nodePanel.Children.Add(label);
        }
        _nodePanel.Children.Add(Label(root ? "What the merchant says first" : "What the merchant says then"));
        var line = new TextBox { Text = node.Line, Width = 560, Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, HorizontalAlignment = HorizontalAlignment.Left };
        line.LostFocus += (_, _) => node.Line = line.Text;
        _nodePanel.Children.Add(line);
        _nodePanel.Children.Add(Note("Its choices follow: the choices under it (left), then its goods below - each by its name - and \"Back\" / \"Nothing more\" (Words)."));
        _nodePanel.Children.Add(Label("Its goods"));
        var rows = new StackPanel();
        void ShowItems()
        {
            rows.Children.Clear();
            foreach (var item in node.Items.ToList())
            {
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(ThingCombo(item.Category, item.ItemId, (c, id) => { item.Category = c; item.ItemId = id; RefreshTreeHeaderOnly(); }));
                var infinite = new CheckBox { Content = "Never runs out", IsChecked = item.Infinite, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                var quantity = Number(() => item.Quantity, v => item.Quantity = Math.Max(0, v));
                quantity.IsEnabled = !item.Infinite;
                infinite.Checked += (_, _) => { item.Infinite = true; quantity.IsEnabled = false; };
                infinite.Unchecked += (_, _) => { item.Infinite = false; quantity.IsEnabled = true; };
                row.Children.Add(infinite);
                row.Children.Add(Small("how many"));
                row.Children.Add(quantity);
                row.Children.Add(Small("price"));
                row.Children.Add(Number(() => item.Price, v => item.Price = Math.Max(0, v)));
                var own = new TextBox { Text = item.PriceLine, Width = 300, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Its own price line ({item}, {price}, {money}) - empty: the shop's (Words)" };
                own.LostFocus += (_, _) => item.PriceLine = own.Text;
                row.Children.Add(Small("own price line"));
                row.Children.Add(own);
                row.Children.Add(Button("✕", () => { node.Items.Remove(item); ShowItems(); RefreshTreeHeaderOnly(); }));
                rows.Children.Add(row);
            }
        }
        ShowItems();
        _nodePanel.Children.Add(rows);
        _nodePanel.Children.Add(Button("Add goods", () => { node.Items.Add(new ShopItem { ItemId = _things.FirstOrDefault()?.Id ?? -1 }); ShowItems(); RefreshTreeHeaderOnly(); }));
    }

    private void RefreshTreeHeaderOnly()
    {
        void Walk(TreeViewItem item)
        {
            if (item.Tag is ShopNode node && !ReferenceEquals(node, _shop.Root)) item.Header = $"{node.Label}  ({node.Items.Count} goods)";
            foreach (var child in item.Items.OfType<TreeViewItem>()) Walk(child);
        }
        foreach (var top in _tree.Items.OfType<TreeViewItem>()) Walk(top);
    }

    // ---------- Buys ----------

    private FrameworkElement BuysTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(Note("What the merchant buys from the party, and what he pays for one - offered under the first line's \"sell\" choice (Words)."));
        var rows = new StackPanel();
        void Show()
        {
            rows.Children.Clear();
            foreach (var buy in _shop.Buys.ToList())
            {
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(ThingCombo(buy.Category, buy.ItemId, (c, id) => { buy.Category = c; buy.ItemId = id; }));
                row.Children.Add(Small("he pays"));
                row.Children.Add(Number(() => buy.Price, v => buy.Price = Math.Max(0, v)));
                row.Children.Add(Button("✕", () => { _shop.Buys.Remove(buy); Show(); }));
                rows.Children.Add(row);
            }
        }
        Show();
        panel.Children.Add(rows);
        panel.Children.Add(Button("Add a thing he buys", () => { _shop.Buys.Add(new ShopBuy { ItemId = _things.FirstOrDefault()?.Id ?? -1 }); Show(); }));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ---------- Haggle ----------

    private FrameworkElement HaggleTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        var on = new CheckBox { Content = "He haggles (Serpent Isle): the player may offer less", IsChecked = _shop.Haggle };
        on.Checked += (_, _) => _shop.Haggle = true;
        on.Unchecked += (_, _) => _shop.Haggle = false;
        panel.Children.Add(on);
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Small("How many offers before he'll haggle no more"));
        row.Children.Add(Number(() => _shop.HaggleTries, v => _shop.HaggleTries = Math.Clamp(v, 1, 20)));
        row.Children.Add(Small("The lowest he'll go (percent of his price)"));
        row.Children.Add(Number(() => _shop.LowestPercent, v => _shop.LowestPercent = Math.Clamp(v, 1, 100)));
        panel.Children.Add(row);
        panel.Children.Add(Note("Haggle opens the slider (System Settings > Slider), from 1 to his asking price - the player slides to an offer and accepts it. An offer at or above what he asks: he takes it at his price. Below his lowest: he's insulted. In between: he meets the player half way (and takes the offer once that's within one of it). Each refusal or counter is an offer used; when they're used up he names his last price - yes or no."));
        return panel;
    }

    // ---------- Merchant ----------

    private FrameworkElement MerchantTab(List<WorldObject> objects)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(Label("Who talks"));
        var speakers = new List<(string Key, string Name)> { ("Self", "(Self - the character this event belongs to, or who set it off)"), ("Player", "(Main Player)"), ("", "(None - narration)") };
        speakers.AddRange(ProjectSession.Characters.Characters.Select(c => (c.Id.ToString(), c.Name)));
        var who = new ComboBox { Width = 420, ItemsSource = speakers.Select(s => s.Name).ToList(), SelectedIndex = Math.Max(0, speakers.FindIndex(s => s.Key == _shop.SpeakerId)), HorizontalAlignment = HorizontalAlignment.Left };
        who.SelectionChanged += (_, _) => _shop.SpeakerId = speakers[Math.Max(0, who.SelectedIndex)].Key;
        panel.Children.Add(who);
        panel.Children.Add(Label("The money"));
        var money = objects.Where(o => o.Type == ObjectType.Money).OrderBy(o => o.Name).ToList();
        var currency = new ComboBox { Width = 300, ItemsSource = money, DisplayMemberPath = nameof(WorldObject.Name), SelectedItem = money.FirstOrDefault(o => o.Id == _shop.CurrencyObjectId) ?? money.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Left };
        if (currency.SelectedItem is WorldObject first) _shop.CurrencyObjectId = first.Id;
        currency.SelectionChanged += (_, _) => _shop.CurrencyObjectId = (currency.SelectedItem as WorldObject)?.Id ?? -1;
        panel.Children.Add(currency);
        if (money.Count == 0) panel.Children.Add(Note("There's no Money object yet - make one in the Object Editor (type Money)."));
        panel.Children.Add(Note("Bought: paid from the whole party's money; carried by the main character, or anyone in the party who can. Sold to him: taken from the party, the money to them (at the main character's feet if nobody can carry it)."));
        return panel;
    }

    // ---------- Words ----------

    private FrameworkElement WordsTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(Note("Every word said in the shop - nothing is fixed in the engine. {item} is the thing's name, {price} the price, {money} the money's name."));
        var names = new Dictionary<string, string>
        {
            [nameof(ShopTexts.PriceLine)] = "Asking the price", [nameof(ShopTexts.Yes)] = "The player's yes", [nameof(ShopTexts.No)] = "The player's no", [nameof(ShopTexts.HaggleChoice)] = "The player's haggle",
            [nameof(ShopTexts.Agreed)] = "Bought", [nameof(ShopTexts.Declined)] = "The player said no", [nameof(ShopTexts.NotEnoughMoney)] = "Not enough money", [nameof(ShopTexts.OutOfStock)] = "None left",
            [nameof(ShopTexts.CantCarry)] = "Nobody can carry it", [nameof(ShopTexts.Back)] = "The player's back (a step up)", [nameof(ShopTexts.Leave)] = "The player's leaving", [nameof(ShopTexts.Farewell)] = "Farewell",
            [nameof(ShopTexts.SellChoice)] = "The player's sell choice", [nameof(ShopTexts.SellLine)] = "What will you sell?", [nameof(ShopTexts.SellOffer)] = "His offer for it", [nameof(ShopTexts.SellAgreed)] = "Sold to him",
            [nameof(ShopTexts.NothingToSell)] = "Nothing he wants", [nameof(ShopTexts.HagglePrompt)] = "Asking for the offer (over the slider)", [nameof(ShopTexts.HaggleTooLow)] = "Too low", [nameof(ShopTexts.HaggleCounter)] = "His counter-offer",
            [nameof(ShopTexts.HaggleAccept)] = "He takes the offer", [nameof(ShopTexts.HaggleGiveUp)] = "No more haggling"
        };
        foreach (var property in typeof(ShopTexts).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBlock { Text = names.TryGetValue(property.Name, out var n) ? n : property.Name, Width = 210, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            var box = new TextBox { Text = property.GetValue(_shop.Texts) as string ?? "" };
            var p = property;
            box.LostFocus += (_, _) => p.SetValue(_shop.Texts, box.Text);
            row.Children.Add(box);
            panel.Children.Add(row);
        }
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ---------- Little builders ----------

    private ComboBox ThingCombo(ReferencedItemCategory category, int id, Action<ReferencedItemCategory, int> set)
    {
        var combo = new ComboBox { Width = 220, ItemsSource = _things, DisplayMemberPath = nameof(Thing.Name), SelectedItem = _things.FirstOrDefault(t => t.Category == category && t.Id == id), VerticalAlignment = VerticalAlignment.Center };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Thing t) set(t.Category, t.Id); };
        return combo;
    }

    private static TextBox Number(Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 55, Text = get().ToString(), VerticalAlignment = VerticalAlignment.Center };
        box.LostFocus += (_, _) => { if (int.TryParse(box.Text.Trim(), out int v)) set(v); box.Text = get().ToString(); };
        return box;
    }

    private static Button Button(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) };
    private static TextBlock Small(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 2, 0, 4) };
}
