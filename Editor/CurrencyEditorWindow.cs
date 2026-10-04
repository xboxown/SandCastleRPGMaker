using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Currency Editor: the kinds of money in the game and what each is
/// worth, in one base unit - Ultima VII has gold coins, gold nuggets and gold
/// bars; Serpent Isle adds filari, monetari and guilders. A Money object
/// (Object Editor) is one of these; a stack of them is worth Value x how many.
/// Exchanging one for another (a money changer) works from these values.
/// </summary>
public class CurrencyEditorWindow : Window
{
    private readonly CurrencyDatabase _db;
    private readonly ListBox _list = new() { DisplayMemberPath = nameof(Currency.Name), Margin = new Thickness(6) };
    private readonly TextBox _name = new() { Width = 200 };
    private readonly TextBox _value = new() { Width = 100 };
    private readonly TextBlock _example = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _details = new() { Margin = new Thickness(10) };

    public CurrencyEditorWindow(CurrencyDatabase currencies)
    {
        _db = currencies;
        Title = "Currency Editor";
        Width = 620;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var add = new Button { Content = "New Currency", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        add.Click += (_, _) =>
        {
            var currency = new Currency { Id = _db.GetNextId(), Name = _db.Currencies.Count == 0 ? "Gold" : "New Currency", Value = 1 };
            _db.Currencies.Add(currency);
            Refresh(currency);
        };
        var delete = new Button { Content = "Delete", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        delete.Click += (_, _) =>
        {
            if (_list.SelectedItem is not Currency currency) return;
            if (MessageBox.Show($"Delete \"{currency.Name}\"? Money objects of it will have no currency.", "Currency Editor", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _db.Currencies.Remove(currency);
            Refresh(null);
        };
        var toolbar = new ToolBar();
        toolbar.Items.Add(add);
        toolbar.Items.Add(delete);

        _details.Children.Add(new TextBlock { Text = "Name" });
        _details.Children.Add(_name);
        _details.Children.Add(new TextBlock { Text = "Value (in the base unit)", Margin = new Thickness(0, 8, 0, 0) });
        _details.Children.Add(_value);
        _details.Children.Add(_example);
        _details.Children.Add(new TextBlock
        {
            Text = "Give one currency a value of 1 - the base unit - and the others what they're worth in it: e.g. Gold coin 1, Gold nugget 30, Gold bar 100; or Copper 1, Silver 10, Gold 100.",
            Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });
        _name.LostFocus += (_, _) => { if (_list.SelectedItem is Currency c && _name.Text.Trim().Length > 0) { c.Name = _name.Text.Trim(); Refresh(c); } };
        _value.LostFocus += (_, _) => { if (_list.SelectedItem is Currency c && double.TryParse(_value.Text, out double v) && v > 0) { c.Value = v; ShowExample(c); } };
        _list.SelectionChanged += (_, _) => ShowSelected();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(_list);
        Grid.SetColumn(_details, 1);
        grid.Children.Add(_details);
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(grid);
        Content = root;
        Refresh(_db.Currencies.FirstOrDefault());
    }

    private void Refresh(Currency? select)
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _db.Currencies;
        _list.SelectedItem = select;
        ShowSelected();
    }

    private void ShowSelected()
    {
        _details.IsEnabled = _list.SelectedItem is Currency;
        if (_list.SelectedItem is not Currency c) { _name.Text = _value.Text = ""; _example.Text = ""; return; }
        _name.Text = c.Name;
        _value.Text = c.Value.ToString("0.####");
        ShowExample(c);
    }

    private void ShowExample(Currency c)
    {
        var others = _db.Currencies.Where(o => o != c).Select(o => $"1 {c.Name} = {c.Value / o.Value:0.####} {o.Name}").ToList();
        _example.Text = others.Count == 0 ? "" : "Exchange: " + string.Join(";  ", others);
    }
}
