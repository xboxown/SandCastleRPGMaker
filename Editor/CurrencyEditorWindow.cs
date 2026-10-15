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
    /// <summary>This currency's exchange rates with each other one.</summary>
    private readonly StackPanel _rates = new() { Margin = new Thickness(0, 6, 0, 0) };

    public CurrencyEditorWindow(CurrencyDatabase currencies)
    {
        _db = currencies;
        Title = "Currency Editor";
        Width = 760;
        Height = 560;
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
            _db.Rates.RemoveAll(r => r.FromId == currency.Id || r.ToId == currency.Id);
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
        _details.Children.Add(new TextBlock { Text = "Exchange rates (a money changer: the Exchange Currency event command)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 0) });
        _details.Children.Add(new TextBlock
        {
            Text = "Each land can have its own money: what one of this buys of each other currency there, and what the money changer keeps. Left empty, it changes by the values above. Changing back uses the other currency's own rate, if it has one.",
            Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 11
        });
        _details.Children.Add(_rates);
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
        ShowRates(c);
    }

    private void ShowRates(Currency c)
    {
        _rates.Children.Clear();
        foreach (var other in _db.Currencies.Where(o => o != c))
        {
            var o = other;
            var rate = _db.Rates.FirstOrDefault(r => r.FromId == c.Id && r.ToId == o.Id);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = $"1 {c.Name} buys", VerticalAlignment = VerticalAlignment.Center, Width = 130 });
            var rateBox = new TextBox { Width = 70, Text = rate is { Rate: > 0 } ? rate.Rate.ToString("0.####") : "", ToolTip = $"Empty: by value ({c.Value / o.Value:0.####})" };
            row.Children.Add(rateBox);
            row.Children.Add(new TextBlock { Text = $" {o.Name};  the changer keeps", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            var cutBox = new TextBox { Width = 40, Text = rate is null ? "0" : rate.CutPercent.ToString("0.##") };
            row.Children.Add(cutBox);
            row.Children.Add(new TextBlock { Text = " %", VerticalAlignment = VerticalAlignment.Center });
            void Commit()
            {
                var existing = _db.Rates.FirstOrDefault(r => r.FromId == c.Id && r.ToId == o.Id);
                double.TryParse(cutBox.Text.Trim(), out double cut);
                if (!double.TryParse(rateBox.Text.Trim(), out double value) || value <= 0)
                {
                    if (existing is not null && cut <= 0) _db.Rates.Remove(existing); // by value, no cut
                    else if (existing is not null) { existing.Rate = c.Value / o.Value; existing.CutPercent = Math.Clamp(cut, 0, 100); }
                    else if (cut > 0) _db.Rates.Add(new ExchangeRate { FromId = c.Id, ToId = o.Id, Rate = c.Value / o.Value, CutPercent = Math.Clamp(cut, 0, 100) });
                    return;
                }
                if (existing is null) _db.Rates.Add(existing = new ExchangeRate { FromId = c.Id, ToId = o.Id });
                existing.Rate = value;
                existing.CutPercent = Math.Clamp(cut, 0, 100);
            }
            rateBox.LostFocus += (_, _) => Commit();
            cutBox.LostFocus += (_, _) => Commit();
            _rates.Children.Add(row);
        }
        if (_rates.Children.Count == 0) _rates.Children.Add(new TextBlock { Text = "(make another currency to set a rate)", Opacity = 0.6 });
    }

    private void ShowExample(Currency c)
    {
        var others = _db.Currencies.Where(o => o != c).Select(o => $"1 {c.Name} = {c.Value / o.Value:0.####} {o.Name}").ToList();
        _example.Text = others.Count == 0 ? "" : "Exchange: " + string.Join(";  ", others);
    }
}
