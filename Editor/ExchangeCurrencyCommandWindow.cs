using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Exchange Currency (a money changer): the party's money of one currency
/// is changed into another at the Currency Editor's exchange rate, the
/// changer keeping its cut - going from one land to the next, you gain or
/// lose on the way.
/// </summary>
public class ExchangeCurrencyCommandWindow : FactionCommandWindowBase
{
    private readonly ComboBox _from, _to;
    private readonly RadioButton _all, _some;
    private readonly TextBox _amount;

    public ExchangeCurrencyCommandWindow(EventCommand? existing = null) : base("Exchange Currency")
    {
        var currencies = ProjectSession.Currencies.Currencies.ToList();
        AddLabel("Change the party's");
        _from = new ComboBox { ItemsSource = currencies, DisplayMemberPath = nameof(Currency.Name) };
        _from.SelectedItem = currencies.FirstOrDefault(c => c.Id.ToString() == existing?.Get("FromId")) ?? currencies.FirstOrDefault();
        Rows.Children.Add(_from);
        AddLabel("into");
        _to = new ComboBox { ItemsSource = currencies, DisplayMemberPath = nameof(Currency.Name) };
        _to.SelectedItem = currencies.FirstOrDefault(c => c.Id.ToString() == existing?.Get("ToId")) ?? currencies.Skip(1).FirstOrDefault() ?? currencies.FirstOrDefault();
        Rows.Children.Add(_to);
        AddLabel("How much");
        _all = new RadioButton { Content = "All of it", GroupName = "amount", IsChecked = existing?.Get("Amount") is not { Length: > 0 } a || a == "All" };
        _some = new RadioButton { Content = "This many:", GroupName = "amount", IsChecked = _all.IsChecked != true, VerticalAlignment = VerticalAlignment.Center };
        _amount = new TextBox { Width = 70, Margin = new Thickness(6, 0, 0, 0), Text = existing?.Get("Amount") is { Length: > 0 } n && n != "All" ? n : "100" };
        Rows.Children.Add(_all);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        line.Children.Add(_some);
        line.Children.Add(_amount);
        Rows.Children.Add(line);
        AddLabel("At the rate set in the Currency Editor (Exchange rates), less the money changer's cut. No money of it: nothing happens (a Conditional Branch can check first).", bold: false);
    }

    protected override bool TryBuild()
    {
        if (_from.SelectedItem is not Currency from || _to.SelectedItem is not Currency to) { MessageBox.Show(this, "Make the currencies in the Currency Editor first.", Title); return false; }
        if (from.Id == to.Id) { MessageBox.Show(this, "Pick two different currencies.", Title); return false; }
        string amount = "All";
        if (_some.IsChecked == true)
        {
            if (!int.TryParse(_amount.Text.Trim(), out int n) || n <= 0) { MessageBox.Show(this, "How many: a whole number above 0.", Title); return false; }
            amount = n.ToString();
        }
        var command = new EventCommand { Type = EventCommandType.ExchangeCurrency };
        command.Parameters["FromId"] = from.Id.ToString();
        command.Parameters["ToId"] = to.Id.ToString();
        command.Parameters["FromName"] = from.Name;
        command.Parameters["ToName"] = to.Name;
        command.Parameters["Amount"] = amount;
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) =>
        $"{(command.Get("Amount") == "All" ? "all" : command.Get("Amount"))} {command.Get("FromName")} into {command.Get("ToName")}";
}
